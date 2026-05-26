using System.IO.Compression;

namespace ModManager.Core;

/// <summary>Outcome of a single archive drop through the save-mod fast-path.</summary>
public enum SaveModDropOutcome { Installed, NotASaveMod, Failed }

/// <summary>One archive's verdict + the world GUID (when installed) + a reason (when failed).</summary>
public sealed record SaveModDropVerdict(
    string SourcePath, SaveModDropOutcome Outcome, string? WorldGuid, string? Reason);

/// <summary>
/// Drop-time orchestrator over <see cref="SaveModDetect"/> + <see cref="SaveModInstaller"/> +
/// <see cref="SaveModStore"/>. Per archive: detect, then install + record, OR pass through as
/// NotASaveMod. Non-archive paths short-circuit to NotASaveMod (the caller's regular intake
/// keeps owning loose files / non-save zips). Pure System.IO; no Electron / UI.
/// </summary>
public static class SaveModFlow
{
    public static IReadOnlyList<SaveModDropVerdict> TryHandleDrops(
        IEnumerable<string> paths,
        IReadOnlyList<string> saveTypeExtensions,
        string saveProfilesDir,
        string snapshotsDir,
        string dataDir,
        string? saveModPath,
        IReadOnlyList<string>? forbidden)
    {
        var verdicts = new List<SaveModDropVerdict>();
        foreach (var p in paths ?? Enumerable.Empty<string>())
            verdicts.Add(Handle(p, saveTypeExtensions, saveProfilesDir, snapshotsDir, dataDir, saveModPath, forbidden));
        return verdicts;
    }

    private static SaveModDropVerdict Handle(
        string path,
        IReadOnlyList<string> saveTypeExtensions,
        string saveProfilesDir,
        string snapshotsDir,
        string dataDir,
        string? saveModPath,
        IReadOnlyList<string>? forbidden)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !IsArchive(path))
            return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, null);

        IReadOnlyList<string> names;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            names = zip.Entries.Select(e => e.FullName).ToList();
        }
        catch (Exception e)
        {
            // Unreadable as a zip (.7z / .rar / corrupt): leave it to the regular intake path.
            return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, e.Message);
        }

        var verdict = SaveModDetect.Detect(names, saveTypeExtensions);
        if (!verdict.IsSaveMod) return new SaveModDropVerdict(path, SaveModDropOutcome.NotASaveMod, null, null);
        if (string.IsNullOrEmpty(verdict.WorldGuid))
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, null,
                "Save mod detected but no world GUID - only Worlds/<GUID> packages auto-install for now.");

        try
        {
            SaveModInstaller.InstallWorld(
                saveProfilesDir, snapshotsDir, dataDir,
                path, verdict.WorldGuid!, saveModPath, forbidden);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            SaveModStore.Upsert(dataDir, new SaveModEntry(verdict.WorldGuid!, name, path, DateTime.UtcNow));
            return new SaveModDropVerdict(path, SaveModDropOutcome.Installed, verdict.WorldGuid, null);
        }
        catch (Exception e)
        {
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, verdict.WorldGuid, e.Message);
        }
    }

    private static bool IsArchive(string p)
    {
        var lower = p.ToLowerInvariant();
        return Intake.ArchiveExtensions.Any(a => lower.EndsWith(a));
    }
}
