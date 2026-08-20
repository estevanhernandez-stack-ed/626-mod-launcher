using System.IO.Compression;
using System.Text.Json;

namespace ModManager.Core.Transport;

/// <summary>How much of a save a bundle carries.</summary>
public enum BundleScope
{
    /// <summary>Everything of yours, minus secrets. Moving between your own machines — your character
    /// travels with you. Needs no per-game knowledge, so it works for every game.</summary>
    Portable,

    /// <summary>The world, without your character or any secret. What you would post publicly.
    /// <b>Requires knowing where the world/character seam is for that game</b>, which is per-game and
    /// not guessable — so it is refused rather than approximated. See the transport plan.</summary>
    Shareable,
}

/// <summary>Which game this save belongs to. Enough to refuse a mismatched restore.</summary>
public sealed record BundleGame(string Id, string? SteamAppId, string? Name);

/// <summary>A file left out of the bundle, and why. An honest artifact says what it is missing.</summary>
public sealed record BundleExclusion(string Path, string Reason);

/// <summary>One mod the save was made with. The part no general-purpose save tool can produce.</summary>
public sealed record BundleMod(string Name, string? Version, int? NexusModId, bool Enabled);

/// <summary>
/// What a bundle would contain, decided but not yet written.
///
/// <para>Separating the decision from the writing is what lets one game's bundle stand alone as a
/// file <b>and</b> sit inside a bigger archive alongside eleven others, without two code paths that
/// have to agree about credentials, exclusions and the manifest. One format, tested once.</para>
/// </summary>
public sealed record BundlePlan(
    SaveBundleManifest Manifest,
    IReadOnlyList<BundlePlanFile> Files);

/// <param name="Full">Absolute source path on this machine.</param>
/// <param name="Relative">Where it goes inside the bundle, under the payload prefix.</param>
public sealed record BundlePlanFile(string Full, string Relative);

/// <summary>
/// What a bundle declares about itself, stored as <c>bundle.json</c> inside the zip.
/// </summary>
public sealed record SaveBundleManifest
{
    public int BundleVersion { get; init; } = SaveBundle.CurrentVersion;
    public BundleGame Game { get; init; } = new("", null, null);
    public string Scope { get; init; } = nameof(BundleScope.Portable).ToLowerInvariant();
    public string CreatedUtc { get; init; } = "";
    public int FileCount { get; init; }
    public long Bytes { get; init; }
    public IReadOnlyList<BundleExclusion> Excluded { get; init; } = Array.Empty<BundleExclusion>();
    public IReadOnlyList<BundleMod> Mods { get; init; } = Array.Empty<BundleMod>();
}

/// <summary>
/// Moving a save between machines without the cloud — carrying <b>what the save needs to run</b>,
/// not just its bytes.
///
/// <para>That last part is the whole reason this lives in a mod manager. Ludusavi already backs saves
/// up and does it well. What it cannot know is that a world was built with nine mods and which three
/// are missing on the machine you just restored it to. Cyberpunk records <c>isModded</c> in every
/// save because the studio knows it matters; we are the only tool in the chain that knows WHICH.</para>
///
/// <para><b>Secrets never travel.</b> Not as a policy exception — an artifact meant to leave the
/// machine cannot contain them. See <see cref="CredentialScan"/>. That is also why a snapshot taken
/// for yourself (<c>SaveManager.Backup</c>) is untouched and stays byte-faithful: those are different
/// jobs, and only one of them leaves.</para>
///
/// <para>Layout inside the zip: <c>bundle.json</c> at the root, the save tree under <c>save/</c>.</para>
/// </summary>
public static class SaveBundle
{
    public const int CurrentVersion = 1;
    public const string ManifestEntry = "bundle.json";
    public const string PayloadPrefix = "save/";
    public const string Extension = ".626save";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Pack a save folder into a bundle.
    ///
    /// <para>Reads only. The source is never modified, which is what makes this safe to offer next to
    /// the destructive operations in the same panel.</para>
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The save folder is not there.</exception>
    /// <exception cref="NotSupportedException"><see cref="BundleScope.Shareable"/> — it needs the
    /// world/character seam for that game, which is per-game data we do not have yet. Refused rather
    /// than approximated: guessing the split ships a stranger someone's character.</exception>
    public static SaveBundleManifest Create(
        string saveDir,
        string bundlePath,
        BundleGame game,
        DateTime createdUtc,
        BundleScope scope = BundleScope.Portable,
        IReadOnlyList<BundleMod>? mods = null)
    {
        var plan = Plan(saveDir, game, createdUtc, scope, mods);

        // Temp-then-rename, so a half-written bundle never appears under its final name.
        var tmp = bundlePath + ".tmp";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bundlePath)!);
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            WriteInto(zip, plan);

        File.Move(tmp, bundlePath, overwrite: true);
        return plan.Manifest;
    }

    /// <summary>
    /// Decide what a bundle would hold, without writing anything.
    ///
    /// <para>Reads only. Every judgement that matters — which files are credentials, what the manifest
    /// says, what was left out — happens here, so a bundle written on its own and a bundle written
    /// into a profile archive cannot disagree.</para>
    /// </summary>
    public static BundlePlan Plan(
        string saveDir,
        BundleGame game,
        DateTime createdUtc,
        BundleScope scope = BundleScope.Portable,
        IReadOnlyList<BundleMod>? mods = null)
    {
        if (scope == BundleScope.Shareable)
            throw new NotSupportedException(
                "A shareable bundle has to know which files are your character and which are the " +
                "world, and that is different for every game. Only a portable bundle - everything of " +
                "yours - can be made right now.");

        if (!Directory.Exists(saveDir))
            throw new DirectoryNotFoundException($"Save folder not found: {saveDir}");

        var excluded = new List<BundleExclusion>();
        var included = new List<(string Full, string Relative)>();

        foreach (var full in Directory.EnumerateFiles(saveDir, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(saveDir, full).Replace('\\', '/');
            if (CredentialScan.IsCredentialFile(full))
            {
                excluded.Add(new BundleExclusion(rel, CredentialScan.Reason));
                continue;
            }
            included.Add((full, rel));
        }

        long bytes = 0;
        foreach (var (full, _) in included)
        {
            try { bytes += new FileInfo(full).Length; } catch { /* counted as zero rather than failing */ }
        }

        return new BundlePlan(
            new SaveBundleManifest
            {
                Game = game,
                Scope = scope.ToString().ToLowerInvariant(),
                CreatedUtc = createdUtc.ToString("O"),
                FileCount = included.Count,
                Bytes = bytes,
                Excluded = excluded,
                Mods = mods ?? Array.Empty<BundleMod>(),
            },
            included.Select(x => new BundlePlanFile(x.Full, x.Relative)).ToList());
    }

    /// <summary>
    /// Write a planned bundle into an open archive, optionally under a prefix.
    ///
    /// <para>Prefix empty: the bundle IS the file — <c>bundle.json</c> at the root. Prefix
    /// <c>games/palworld/</c>: the same bundle sits inside a profile archive next to eleven others.
    /// Same writer, same manifest, same exclusions.</para>
    /// </summary>
    public static void WriteInto(ZipArchive zip, BundlePlan plan, string prefix = "")
    {
        prefix = Normalise(prefix);

        var entry = zip.CreateEntry(prefix + ManifestEntry);
        using (var w = new StreamWriter(entry.Open()))
            w.Write(JsonSerializer.Serialize(plan.Manifest, Json));

        foreach (var f in plan.Files)
            zip.CreateEntryFromFile(f.Full, prefix + PayloadPrefix + f.Relative);
    }

    /// <summary>A prefix is either empty or ends in a single forward slash. Nothing else is a prefix,
    /// and a caller getting it slightly wrong would silently produce entries nothing can find.</summary>
    private static string Normalise(string? prefix)
    {
        var p = (prefix ?? "").Replace('\\', '/').Trim('/');
        return p.Length == 0 ? "" : p + "/";
    }

    /// <summary>Read a bundle's manifest without extracting anything — so the app can say what is in
    /// it, and which mods are missing, before touching a single save file.</summary>
    public static SaveBundleManifest? ReadManifest(string bundlePath, string prefix = "")
    {
        try
        {
            using var zip = ZipFile.OpenRead(bundlePath);
            var entry = zip.GetEntry(Normalise(prefix) + ManifestEntry);
            if (entry is null) return null;
            using var r = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<SaveBundleManifest>(r.ReadToEnd(), Json);
        }
        catch { return null; }
    }

    /// <summary>
    /// Unpack a bundle over a save folder.
    ///
    /// <para><b>Snapshot first, always.</b> This replaces what is there, and the whole point of the
    /// feature is moving saves between machines — the destination may well have a save on it already.
    /// The pre-restore snapshot is the same guarantee the rest of the panel gives, under the same
    /// name.</para>
    ///
    /// <para><b>Refuses a bundle from a different game.</b> Restoring Palworld over Elden Ring would
    /// look like a success and destroy a save. The game id travels in the manifest for exactly this.</para>
    /// </summary>
    public static SaveBundleManifest Restore(string bundlePath, string saveDir, string snapshotsDir,
                                            string gameId, string prefix = "")
    {
        var manifest = ReadManifest(bundlePath, prefix)
            ?? throw new InvalidOperationException(
                "That file is not a save bundle, or its description could not be read. Nothing was changed.");

        if (manifest.BundleVersion > CurrentVersion)
            throw new InvalidOperationException(
                $"This bundle was made by a newer version of the launcher (format {manifest.BundleVersion}). " +
                "Update, then try again - nothing was changed.");

        if (!string.IsNullOrEmpty(manifest.Game.Id)
            && !string.Equals(manifest.Game.Id, gameId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"That bundle is a save for {manifest.Game.Name ?? manifest.Game.Id}. Restoring it here " +
                "would replace a different game's save.");

        Directory.CreateDirectory(saveDir);
        if (Directory.EnumerateFileSystemEntries(saveDir).Any())
            SaveManager.Backup(saveDir, snapshotsDir, "before-restore", auto: true);

        foreach (var f in Directory.GetFiles(saveDir)) File.Delete(f);
        foreach (var d in Directory.GetDirectories(saveDir)) Directory.Delete(d, recursive: true);

        var payload = Normalise(prefix) + PayloadPrefix;
        using (var zip = ZipFile.OpenRead(bundlePath))
        {
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(payload, StringComparison.Ordinal)) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                var rel = entry.FullName[payload.Length..];
                var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(saveDir, rel));

                // Path traversal: a bundle is a file from somewhere else, so it is untrusted input.
                if (!dest.StartsWith(System.IO.Path.GetFullPath(saveDir), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"That bundle tries to write outside the save folder ({rel}). Refused.");

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        return manifest;
    }

    /// <summary>
    /// Where to get a mod the bundle says it needs.
    ///
    /// <para><b>Built here, never carried in the bundle.</b> A bundle arrives from another person and
    /// is untrusted input, so a URL inside it is a link somebody else chose — a phishing page under a
    /// real mod's name is the obvious attack, and it would be rendered next to that mod's name by an
    /// app the user trusts. Constructing it from the numeric mod id and the domain WE resolve for the
    /// game means the link can only ever point at that game's page on Nexus.</para>
    ///
    /// <para>Null when the bundle did not record an id, or the game has no Nexus domain. The UI names
    /// the mod without a link rather than inventing somewhere to send people.</para>
    /// </summary>
    public static string? NexusUrlFor(BundleMod? mod, string? nexusDomain)
    {
        if (mod?.NexusModId is not { } id || id <= 0) return null;
        if (string.IsNullOrWhiteSpace(nexusDomain)) return null;

        // The domain comes from our own manifest, but it is still interpolated into a URL - keep it to
        // the shape Nexus slugs actually take rather than trusting it blind.
        foreach (var c in nexusDomain)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return null;

        return $"https://www.nexusmods.com/{nexusDomain}/mods/{id}";
    }

    /// <summary>Which of a bundle's mods are not installed here, by name. The sentence the import
    /// screen exists to say.</summary>
    public static IReadOnlyList<BundleMod> MissingMods(
        SaveBundleManifest? manifest, IReadOnlyCollection<string>? installedNames)
    {
        if (manifest is null || manifest.Mods.Count == 0) return Array.Empty<BundleMod>();
        var have = new HashSet<string>(installedNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return manifest.Mods.Where(m => !have.Contains(m.Name)).ToList();
    }
}
