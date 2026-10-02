using System.IO.Compression;

namespace ModManager.Core;

/// <summary>Outcome of a single archive drop through the save-mod fast-path. NeedsAcknowledgment:
/// it is a save mod for a game whose save writes are gated, and nothing was written. AlreadyInstalled: 626
/// installed this world and it is still there (reset or remove it). WorldExists: the world is there but 626 didn't
/// install it, or the game already holds it in its own store (it has been played). Nothing was written for either;
/// the reason says what the user can do.</summary>
public enum SaveModDropOutcome { Installed, NotASaveMod, Failed, NeedsAcknowledgment, AlreadyInstalled, WorldExists }

/// <summary>One archive's verdict + the world GUID (when installed) + a reason (when failed).</summary>
public sealed record SaveModDropVerdict(
    string SourcePath, SaveModDropOutcome Outcome, string? WorldGuid, string? Reason);

/// <summary>
/// Drop-time orchestrator over <see cref="SaveModDetect"/> + <see cref="SaveModInstaller"/> +
/// <see cref="SaveModStore"/>. Per archive: detect, then install + record, OR pass through as
/// NotASaveMod. Non-archive paths short-circuit to NotASaveMod (the caller's regular intake
/// keeps owning loose files / non-save zips). Pure System.IO; no Electron / UI.
/// writeAllowed false: a detected save mod returns NeedsAcknowledgment and nothing is written.
/// The caller decides it from BanRiskRules.ShouldGateSaveWrite, asks, and re-runs only those paths.
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
        IReadOnlyList<string>? forbidden,
        bool writeAllowed,
        string? writeRefusal = null)
    {
        var verdicts = new List<SaveModDropVerdict>();
        foreach (var p in paths ?? Enumerable.Empty<string>())
            verdicts.Add(Handle(p, saveTypeExtensions, saveProfilesDir, snapshotsDir, dataDir, saveModPath, forbidden, writeAllowed, writeRefusal));
        return verdicts;
    }

    /// <summary>What a drop of <paramref name="path"/> WOULD be, decided exactly as a real drop decides
    /// it, and nothing is written: NotASaveMod; Failed with the reason (saves the launcher may not write,
    /// or no world GUID); or NeedsAcknowledgment, which here means "a save mod that would install" once
    /// the caller has settled the ban-risk question. The drop router and a real drop share this.</summary>
    public static SaveModDropVerdict Classify(string path, IReadOnlyList<string> saveTypeExtensions, string? writeRefusal)
        => Handle(path, saveTypeExtensions, "", "", "", null, null, writeAllowed: false, writeRefusal);

    private static SaveModDropVerdict Handle(
        string path,
        IReadOnlyList<string> saveTypeExtensions,
        string saveProfilesDir,
        string snapshotsDir,
        string dataDir,
        string? saveModPath,
        IReadOnlyList<string>? forbidden,
        bool writeAllowed,
        string? writeRefusal)
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

        // A save mod for a game whose saves are not the launcher's to write (SaveWritePolicy): still
        // recognised, so regular intake never tries to classify it, and turned away with the reason.
        // No acknowledgement unlocks it - this is not a risk the player can accept for the launcher.
        if (writeRefusal is not null)
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, verdict.WorldGuid, writeRefusal);
        if (string.IsNullOrEmpty(verdict.WorldGuid))
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, null,
                "Save mod detected but no world GUID - only Worlds/<GUID> packages auto-install for now.");

        // Before the ban-risk question: nobody is asked to accept a risk for an install that would be refused. A drop
        // the router only classifies (no save folder given) skips this; the install below runs it again anyway.
        if (!string.IsNullOrEmpty(saveProfilesDir))
        {
            try { SaveModInstaller.PreflightInstall(saveProfilesDir, saveModPath, forbidden, verdict.WorldGuid!); }
            catch (Exception e) when (ExistingWorld(e, dataDir, verdict.WorldGuid!, path) is { } existing) { return existing; }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, verdict.WorldGuid, e.Message);
            }
        }

        if (!writeAllowed)
            return new SaveModDropVerdict(path, SaveModDropOutcome.NeedsAcknowledgment, verdict.WorldGuid, null);

        try
        {
            SaveModInstaller.InstallWorld(
                saveProfilesDir, snapshotsDir, dataDir,
                path, verdict.WorldGuid!, saveModPath, forbidden);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            // The record points at the kept copy, which reset reads: the download may be deleted.
            SaveModStore.Upsert(dataDir, new SaveModEntry(verdict.WorldGuid!, name,
                SaveModInstaller.KeptZipPath(dataDir, verdict.WorldGuid!, path), DateTime.UtcNow));
            return new SaveModDropVerdict(path, SaveModDropOutcome.Installed, verdict.WorldGuid, null);
        }
        catch (Exception e) when (ExistingWorld(e, dataDir, verdict.WorldGuid!, path) is { } existing)
        {
            return existing;
        }
        catch (Exception e)
        {
            return new SaveModDropVerdict(path, SaveModDropOutcome.Failed, verdict.WorldGuid, e.Message);
        }
    }

    // The verdict for a world that is already there: in the game's own store (played), or in the save-mod folder,
    // installed by 626 (reset or remove it) or not (626 won't write over it). Null for any other failure.
    private static SaveModDropVerdict? ExistingWorld(Exception e, string dataDir, string worldGuid, string path) => e switch
    {
        WorldInGameSaveException => new SaveModDropVerdict(path, SaveModDropOutcome.WorldExists, worldGuid, e.Message),
        WorldAlreadyPresentException p => new SaveModDropVerdict(path,
            Installed626(dataDir, worldGuid) ? SaveModDropOutcome.AlreadyInstalled : SaveModDropOutcome.WorldExists,
            worldGuid, AlreadyInstalledReason(dataDir, worldGuid, p.WorldDir)),
        _ => null,
    };

    private static bool Installed626(string dataDir, string worldGuid)
        => !string.IsNullOrEmpty(dataDir)
           && SaveModStore.Load(dataDir).Any(e => string.Equals(e.Guid, worldGuid, StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a world that is already in the save folder wasn't installed, and what to do: reset or remove it
    /// when 626 installed it (it is in the Saves list), else move the folder away, since 626 has no record of it.</summary>
    public static string AlreadyInstalledReason(string dataDir, string worldGuid, string worldDir)
        => Installed626(dataDir, worldGuid)
            ? $"World {worldGuid} is already installed. Nothing was changed. Reset it to start it over from its kept zip, "
              + "or remove it first to install this one."
            : $"A world with id {worldGuid} is already in the save folder ({worldDir}), and 626 didn't install it, so it "
              + "won't write over it. Nothing was changed. Move that folder somewhere else first to install this one.";

    private static bool IsArchive(string p)
    {
        var lower = p.ToLowerInvariant();
        return Intake.ArchiveExtensions.Any(a => lower.EndsWith(a));
    }
}
